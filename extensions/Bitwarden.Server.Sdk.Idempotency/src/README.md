# Bitwarden.Server.Sdk.Idempotency

**Audience:** engineers changing this package.

The middleware and store that do a keyed write from another Bitwarden service at most once. See
[PACKAGE.md](PACKAGE.md) for how to use it.

## What belongs here

The server side of idempotency keys only: deciding which requests are recorded, the record, and its stores. The header
name, the key's rules and what a client sends live in Bitwarden.Server.Sdk.Idempotency.Abstractions, which a client
references without ASP.NET Core. Publishing the mark in a service's contract lives in Bitwarden.Server.Sdk.Service.

## How it works

- **`IdempotentWrites`** is the middleware. It needs the endpoint chosen and the caller authenticated, which is why
  `UseIdempotencyKeys()` goes last. A refusal earlier in the pipeline is never recorded.
- **The record.** `StorageKey` and `Owners` build the key; `FingerprintAsync` buffers the body and rewinds it for the
  operation.
- **Claim, then complete or release.** `IIdempotencyStore.ClaimAsync` stores a pending record with a nonce for
  `Lease`, so only the request that holds it releases it. A completed answer replaces it for `Retention`; a `5xx`, an
  exception or an answer past `MaximumBytes` (`CapturingStream.Overflowed`) releases it. `ClaimOutcome.Unavailable`,
  which the Redis store returns on a `RedisException` or timeout, runs the request without a key.
- **The `409` is written, not thrown,** because the exception handler would clear `Retry-After`.
- **Sealing.** Purpose `Bitwarden.Server.Sdk.Service.Idempotency.v1`; it isn't this package's name, and must stay as it
  is, or every kept answer becomes unopenable.
- **Choosing the store.** `AddIdempotencyKeys` checks `KeyManagementOptions.XmlRepository`.

## Testing

Every type with logic has a test class of the same name in `tests/Bitwarden.Server.Sdk.Idempotency.Tests`. The Redis
store's tests start a Redis container, so they need Docker.

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Idempotency/tests/Bitwarden.Server.Sdk.Idempotency.Tests
```

The middleware is also tested inside a whole service host, in Bitwarden.Server.Sdk.Service's tests. A change must keep:

- **At most once.** A write never runs twice for one caller and key while its record is kept, even across instances
  and when a record can't be opened.
- **Failures never replayed.** Only an answer below `500`, written by the operation, is kept.
- **Records sealed, under the protector's original purpose.**
- **Between services only.** A browser's write is never recorded.
- **Opt-in.** An operation without `[AcceptsIdempotencyKey]` is never recorded, whatever it carries.
